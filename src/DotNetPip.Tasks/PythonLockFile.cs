using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotNetPip.Tasks;

internal sealed class PythonLockFile
{
    public const int CurrentLockVersion = 1;
    public const string DefaultConstraint = "numpy<2";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public int LockVersion { get; set; } = CurrentLockVersion;
    public string CreatedBy { get; set; } = "DotNetPip";
    public string Python { get; set; } = string.Empty;
    public string? IndexUrl { get; set; }
    public List<string> ExtraIndexUrls { get; set; } = [];
    public List<string> Constraints { get; set; } = [];
    public List<PythonLockDirect> Direct { get; set; } = [];
    public List<PythonLockPackage> Packages { get; set; } = [];

    public static List<string> DefaultConstraints() => [DefaultConstraint];

    public static PythonLockFile? TryRead(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<PythonLockFile>(File.ReadAllText(path), JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Write(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
    }

    public bool Matches(
        string python,
        IReadOnlyList<PythonLockDirect> direct,
        IReadOnlyList<string> constraints,
        string? indexUrl,
        IReadOnlyList<string> extraIndexUrls)
    {
        if (LockVersion != CurrentLockVersion)
        {
            return false;
        }

        if (!string.Equals(Python, python, StringComparison.Ordinal))
        {
            return false;
        }

        if (!StringSetEquals(Constraints, constraints))
        {
            return false;
        }

        if (!StringEqualsOrEmpty(IndexUrl, indexUrl))
        {
            return false;
        }

        if (!StringSetEquals(ExtraIndexUrls, extraIndexUrls))
        {
            return false;
        }

        if (Direct.Count != direct.Count)
        {
            return false;
        }

        List<string> expected = direct.Select(DirectKey).OrderBy(s => s, StringComparer.Ordinal).ToList();
        List<string> actual = Direct.Select(DirectKey).OrderBy(s => s, StringComparer.Ordinal).ToList();
        return expected.SequenceEqual(actual, StringComparer.Ordinal);
    }

    public static PythonLockFile FromPipReport(
        string reportJson,
        string python,
        IReadOnlyList<PythonLockDirect> direct,
        IReadOnlyList<string> constraints,
        string? indexUrl,
        IReadOnlyList<string> extraIndexUrls)
    {
        using JsonDocument doc = JsonDocument.Parse(reportJson);
        JsonElement root = doc.RootElement;
        if (!root.TryGetProperty("install", out JsonElement install) || install.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("pip --report did not contain an install array.");
        }

        List<PythonLockPackage> packages = [];
        foreach (JsonElement item in install.EnumerateArray())
        {
            packages.Add(ParseInstallItem(item));
        }

        packages.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        return new PythonLockFile
        {
            Python = python,
            IndexUrl = string.IsNullOrWhiteSpace(indexUrl) ? null : indexUrl,
            ExtraIndexUrls = extraIndexUrls.Where(s => !string.IsNullOrWhiteSpace(s)).ToList(),
            Constraints = constraints.ToList(),
            Direct = direct.ToList(),
            Packages = packages,
        };
    }

    public string ToHashRequirements()
    {
        StringBuilder builder = new();
        foreach (PythonLockPackage package in Packages)
        {
            if (string.IsNullOrWhiteSpace(package.Url) || string.IsNullOrWhiteSpace(package.Sha256))
            {
                throw new InvalidOperationException(
                    $"Lock entry '{package.Name}=={package.Version}' is missing url or sha256.");
            }

            builder.Append(package.Name)
                .Append(" @ ")
                .Append(package.Url)
                .Append(" --hash=sha256:")
                .Append(package.Sha256)
                .Append('\n');
        }

        return builder.ToString();
    }

    public static string CanonicalName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        char[] chars = name.Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (chars[i] is '_' or '.')
            {
                chars[i] = '-';
            }
        }

        return new string(chars);
    }

    public static (string? IndexUrl, List<string> ExtraIndexUrls) IndexesFromEnvironment()
    {
        string? index = EmptyToNull(Environment.GetEnvironmentVariable("PIP_INDEX_URL"));
        List<string> extra = [];
        string? extraRaw = Environment.GetEnvironmentVariable("PIP_EXTRA_INDEX_URL");
        if (!string.IsNullOrWhiteSpace(extraRaw))
        {
            extra.AddRange(extraRaw.Split([';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return (index, extra);
    }

    private static PythonLockPackage ParseInstallItem(JsonElement item)
    {
        if (!item.TryGetProperty("metadata", out JsonElement metadata))
        {
            throw new InvalidOperationException("pip --report install item is missing metadata.");
        }

        string name = metadata.TryGetProperty("name", out JsonElement nameProp)
            ? nameProp.GetString() ?? string.Empty
            : string.Empty;
        string version = metadata.TryGetProperty("version", out JsonElement verProp)
            ? verProp.GetString() ?? string.Empty
            : string.Empty;

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException("pip --report install item is missing name or version.");
        }

        if (!item.TryGetProperty("download_info", out JsonElement download)
            || download.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException(
                $"pip --report did not include download info for '{name}=={version}'. Hash-verified restore requires a wheel or sdist URL.");
        }

        string url = download.TryGetProperty("url", out JsonElement urlProp)
            ? urlProp.GetString() ?? string.Empty
            : string.Empty;
        string sha256 = ReadSha256(download);
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(sha256))
        {
            throw new InvalidOperationException(
                $"pip --report did not include a URL and sha256 for '{name}=={version}'.");
        }

        string filename = Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)
            ? Path.GetFileName(uri.AbsolutePath)
            : Path.GetFileName(url);

        return new PythonLockPackage
        {
            Name = name,
            Version = version,
            Filename = filename,
            Url = url,
            Sha256 = sha256,
        };
    }

    private static string ReadSha256(JsonElement downloadInfo)
    {
        if (!downloadInfo.TryGetProperty("archive_info", out JsonElement archive)
            || archive.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        if (archive.TryGetProperty("hashes", out JsonElement hashes)
            && hashes.ValueKind == JsonValueKind.Object
            && hashes.TryGetProperty("sha256", out JsonElement shaProp))
        {
            return shaProp.GetString() ?? string.Empty;
        }

        if (archive.TryGetProperty("hash", out JsonElement hashProp))
        {
            string? combined = hashProp.GetString();
            const string prefix = "sha256=";
            if (!string.IsNullOrEmpty(combined) && combined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return combined[prefix.Length..];
            }
        }

        return string.Empty;
    }

    private static string DirectKey(PythonLockDirect direct) =>
        CanonicalName(direct.Name) + "==" + (direct.Version ?? string.Empty).Trim();

    private static bool StringEqualsOrEmpty(string? left, string? right) =>
        string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);

    private static bool StringSetEquals(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        HashSet<string> a = new(left.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
        HashSet<string> b = new(right.Where(s => !string.IsNullOrWhiteSpace(s)), StringComparer.Ordinal);
        return a.SetEquals(b);
    }

    private static string? EmptyToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal sealed class PythonLockDirect
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}

internal sealed class PythonLockPackage
{
    public string Name { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string? Filename { get; set; }
    public string Url { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}
