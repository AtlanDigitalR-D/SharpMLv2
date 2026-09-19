using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SharpML2.Models;

namespace SharpML2.Services;

public sealed class JevClassifier : ISecretClassifier, IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string _model;

    public JevClassifier(string endpoint, string? apiKey, string model, HttpMessageHandler? handler = null)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed))
        {
            throw new ArgumentException("Jev endpoint must be an absolute URL.", nameof(endpoint));
        }

        _endpoint = parsed;
        _model = model;
        _httpClient = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _httpClient.Timeout = TimeSpan.FromSeconds(15);
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SharpML2/0.1");

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
    }

    public async Task<ClassificationResult> ClassifyAsync(CandidateSecret candidate, CancellationToken cancellationToken)
    {
        var payload = new
        {
            model = _model,
            state = BuildRedactedState(candidate),
            questions = new Dictionary<string, object>
            {
                ["secret_type"] = new
                {
                    type = "choice",
                    instructions = "What kind of sensitive authentication material does this record most likely represent? Choose non_secret when the evidence is more consistent with documentation, examples, placeholders, or ordinary non-secret data.",
                    criteria = new Dictionary<string, string>
                    {
                        ["password"] = "A password or passphrase used for authentication",
                        ["api_key"] = "An API key, cloud access key, application secret, or similar credential",
                        ["token"] = "An access, bearer, session, or personal access token",
                        ["connection_string"] = "A service or database connection string containing authentication material",
                        ["private_key"] = "A private cryptographic key",
                        ["non_secret"] = "Not genuine sensitive authentication material",
                        ["uncertain"] = "There is not enough evidence to classify it reliably"
                    }
                },
                ["likely_real"] = new
                {
                    type = "noul",
                    instructions = "Given the detector evidence, masked characteristics, path hints and surrounding redacted context, is this likely to be a real deployed secret rather than example, documentation, placeholder or test data?"
                },
                ["priority"] = new
                {
                    type = "score",
                    instructions = "How urgently should a defensive security analyst review this potential secret exposure?",
                    criteria = new[]
                    {
                        "Very low: likely example, placeholder, or harmless test data",
                        "Low: weak evidence of a usable secret",
                        "Moderate: plausible secret exposure requiring review",
                        "High: strong evidence of a real credential in operational context",
                        "Critical: strong evidence of a high-impact production credential or private key exposure"
                    }
                }
            }
        };

        var stopwatch = Stopwatch.StartNew();
        using var response = await _httpClient.PostAsJsonAsync(_endpoint, payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        stopwatch.Stop();
        if (!response.IsSuccessStatusCode)
		{
    			throw new HttpRequestException(
       			 $"Jev returned HTTP {(int)response.StatusCode}: {body}"
   					 );
		}

        using var doc = JsonDocument.Parse(body);
        var answers = GetAnswers(doc.RootElement);

        var secretAnswer = answers.GetProperty("secret_type");
        var likelyAnswer = answers.GetProperty("likely_real");
        var priorityAnswer = answers.GetProperty("priority");

        var secretType = secretAnswer.GetProperty("choice").GetString() ?? "uncertain";
        var likelyReal = likelyAnswer.GetProperty("noul").GetDouble();
        var priority = priorityAnswer.GetProperty("score").GetDouble();

        var confidences = new List<double>();
        if (secretAnswer.TryGetProperty("confidence", out var secretConfidence) && secretConfidence.ValueKind == JsonValueKind.Number)
        {
            confidences.Add(secretConfidence.GetDouble());
        }
        if (priorityAnswer.TryGetProperty("confidence", out var priorityConfidence) && priorityConfidence.ValueKind == JsonValueKind.Number)
        {
            confidences.Add(priorityConfidence.GetDouble());
        }

        var confidence = confidences.Count == 0 ? 0.5 : confidences.Average();

        return new ClassificationResult(
            SecretType: secretType,
            LikelyReal: Math.Clamp(likelyReal, 0, 1),
            PriorityScore: Math.Clamp(priority, 0, 4),
            Confidence: Math.Clamp(confidence, 0, 1),
            Source: "jev"
        );
    }

    private static object BuildRedactedState(CandidateSecret candidate)
    {
        var path = candidate.FilePath.ToLowerInvariant();
        string[] knownHints =
        {
            "prod", "production", "deploy", "terraform", "ansible", "finance", "config",
            "example", "sample", "test", "fixture", "docs", "documentation", "admin"
        };
        var pathHints = knownHints.Where(h => path.Contains(h, StringComparison.Ordinal)).Distinct().ToArray();

        var fileName = Path.GetFileName(candidate.FilePath).ToLowerInvariant();
        string[] knownNameHints =
        {
            "secret", "credential", "password", "passwd", "config", "settings", "database",
            "connection", "deploy", "terraform", "env", "example", "sample", "test"
        };
        var nameHints = knownNameHints.Where(h => fileName.Contains(h, StringComparison.Ordinal)).Distinct().ToArray();

        return new
        {
            extension = candidate.Extension,
            file_size_bytes = candidate.FileSizeBytes,
            line_number = candidate.LineNumber,
            detector = candidate.Detector,
            key_name = candidate.KeyName,
            masked_value = candidate.MaskedValue,
            value_length = candidate.ValueLength,
            entropy = candidate.Entropy,
            looks_placeholder = candidate.LooksPlaceholder,
            path_hints = pathHints,
            file_name_hints = nameHints,
            surrounding_context = candidate.Context,
            last_write_time_utc = candidate.LastWriteTimeUtc
        };
    }

    private static JsonElement GetAnswers(JsonElement root)
    {
        if (root.TryGetProperty("result", out var result) && result.TryGetProperty("answers", out var resultAnswers))
        {
            return resultAnswers;
        }

        if (root.TryGetProperty("answers", out var answers))
        {
            return answers;
        }

        throw new JsonException("Jev response did not contain result.answers or answers.");
    }

    public void Dispose() => _httpClient.Dispose();
}
