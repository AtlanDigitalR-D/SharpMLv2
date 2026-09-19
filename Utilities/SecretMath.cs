using System.Security.Cryptography;
using System.Text;

namespace SharpML2.Utilities;

public static class SecretMath
{
    // Ephemeral key means fingerprints can deduplicate within one process without creating
    // a reusable unsalted hash database of low-entropy passwords.
    private static readonly byte[] FingerprintKey = RandomNumberGenerator.GetBytes(32);

    public static double ShannonEntropy(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        var counts = new Dictionary<char, int>();
        foreach (var c in value)
        {
            counts[c] = counts.TryGetValue(c, out var count) ? count + 1 : 1;
        }

        var length = (double)value.Length;
        var entropy = 0.0;
        foreach (var count in counts.Values)
        {
            var p = count / length;
            entropy -= p * Math.Log2(p);
        }

        return Math.Round(entropy, 3);
    }

    public static string Fingerprint(string value)
    {
        using var hmac = new HMACSHA256(FingerprintKey);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }

    public static string Mask(string value)
        => $"<REDACTED:length={value.Length}>";

    public static bool LooksPlaceholder(string value)
    {
        var normalized = value.Trim().Trim('"', '\'', '`').ToLowerInvariant();
        if (normalized.Length == 0)
        {
            return true;
        }

        string[] exact =
        {
            "password", "passwd", "changeme", "change_me", "example", "example123",
            "test", "test123", "dummy", "placeholder", "your_password", "<password>",
            "${password}", "${secret}", "secret", "none", "null", "xxxxx", "********"
        };

        if (exact.Contains(normalized, StringComparer.Ordinal))
        {
            return true;
        }

        return normalized.Contains("replace_me", StringComparison.Ordinal)
            || normalized.Contains("your-password", StringComparison.Ordinal)
            || normalized.Contains("example", StringComparison.Ordinal)
            || normalized.Contains("placeholder", StringComparison.Ordinal);
    }
}
