using SharpML2.Models;

namespace SharpML2.Services;

public sealed class FileScanner
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".sh", ".py", ".pl",
        ".txt", ".xml", ".yml", ".yaml", ".cfg", ".conf", ".config", ".ini",
        ".json", ".env", ".properties", ".sql", ".tf", ".tfvars", ".toml",
        ".cs", ".js", ".ts", ".java", ".go", ".rb", ".php"
    };

    private readonly SecretDetector _detector;

    public FileScanner(SecretDetector detector)
    {
        _detector = detector;
    }

    public IEnumerable<CandidateSecret> Scan(ScanOptions options, Action<string>? onWarning = null)
    {
        var emitted = 0;
        foreach (var filePath in EnumerateFilesSafe(options.RootPath, onWarning))
        {
            if (emitted >= options.MaxCandidates)
            {
                yield break;
            }

            FileInfo info;
            try
            {
                info = new FileInfo(filePath);
                if (!AllowedExtensions.Contains(info.Extension) || info.Length == 0 || info.Length > options.MaxFileSizeBytes)
                {
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                onWarning?.Invoke($"Skipping metadata read for {filePath}: {ex.Message}");
                continue;
            }

            string[] lines;
            try
            {
                lines = File.ReadAllLines(filePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                onWarning?.Invoke($"Skipping unreadable file {filePath}: {ex.Message}");
                continue;
            }

            foreach (var candidate in _detector.Detect(filePath, info, lines, options.ContextLines))
            {
                yield return candidate;
                emitted++;
                if (emitted >= options.MaxCandidates)
                {
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, Action<string>? onWarning)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var current = pending.Pop();
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                onWarning?.Invoke($"Skipping directory {current}: {ex.Message}");
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                onWarning?.Invoke($"Cannot enumerate subdirectories of {current}: {ex.Message}");
                continue;
            }

            foreach (var directory in directories)
            {
                try
                {
                    var attributes = File.GetAttributes(directory);
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                pending.Push(directory);
            }
        }
    }
}
