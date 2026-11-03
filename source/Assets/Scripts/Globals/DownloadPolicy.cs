using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

public sealed class PackagePaths
{
    public string Root { get; private set; }
    public string FinalPath { get; private set; }
    public string ResumePath { get; private set; }

    public PackagePaths(string root, string finalPath, string resumePath)
    {
        Root = root;
        FinalPath = finalPath;
        ResumePath = resumePath;
    }
}

public sealed class PackageVerification
{
    public bool IsValid { get; private set; }
    public string Error { get; private set; }
    public ulong Length { get; private set; }
    public string Sha256 { get; private set; }

    public PackageVerification(bool isValid, string error, ulong length, string sha256)
    {
        IsValid = isValid;
        Error = error;
        Length = length;
        Sha256 = sha256;
    }
}

public sealed class DownloadProgress
{
    public ulong CurrentBytes { get; private set; }
    public ulong TotalBytes { get; private set; }
    public ulong DeltaBytes { get; private set; }
    public ulong RemainingBytes { get; private set; }
    public ulong SpeedBytesPerSecond { get; private set; }
    public double Percent { get; private set; }
    public bool HasKnownTotal { get; private set; }

    public DownloadProgress(ulong currentBytes, ulong totalBytes, ulong deltaBytes, ulong remainingBytes,
        ulong speedBytesPerSecond, double percent, bool hasKnownTotal)
    {
        CurrentBytes = currentBytes;
        TotalBytes = totalBytes;
        DeltaBytes = deltaBytes;
        RemainingBytes = remainingBytes;
        SpeedBytesPerSecond = speedBytesPerSecond;
        Percent = percent;
        HasKnownTotal = hasKnownTotal;
    }
}

public enum DownloadFailureKind
{
    Transport,
    Http,
    Cancelled,
    LocalValidation
}

public static class DownloadPolicy
{
    public const int MaximumTitleIdLength = 64;
    public const int MaximumFileNameUtf8Bytes = 240;
    public const int MaximumRetryDelaySeconds = 300;

    public static string NormalizeDownloadRoot(string root, bool consoleMode)
    {
        if (String.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Download root is required.", "root");
        if (ContainsControl(root))
            throw new ArgumentException("Download root contains an invalid character.", "root");

        if (consoleMode)
        {
            if (root[0] != '/' || root.IndexOf('\\') >= 0 || root.IndexOf("//", StringComparison.Ordinal) >= 0 || HasDotSegment(root))
                throw new ArgumentException("Download root is not canonical.", "root");
            string normalized = root.TrimEnd('/');
            string[] segments = normalized.Split('/');
            bool allowed = normalized == "/user/data" ||
                           normalized.StartsWith("/user/data/", StringComparison.Ordinal);
            if (!allowed && segments.Length >= 3 && segments[1] == "mnt")
                allowed = IsNumberedMount(segments[2], "usb") || IsNumberedMount(segments[2], "ext");
            if (!allowed)
                throw new ArgumentException("Download root is outside the console allowlist.", "root");
            return normalized + "/";
        }

        if (HasDotSegment(root.Replace('\\', '/')))
            throw new ArgumentException("Download root contains traversal.", "root");
        string full = Path.GetFullPath(root);
        string pathRoot = Path.GetPathRoot(full);
        if (String.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            pathRoot == null ? null : pathRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A filesystem root cannot be used as the download root.", "root");
        return full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
    }

    public static string NormalizeTitleId(string titleId)
    {
        if (String.IsNullOrEmpty(titleId) || titleId.Length > MaximumTitleIdLength)
            throw new ArgumentException("Title ID length is invalid.", "titleId");
        for (int i = 0; i < titleId.Length; i++)
        {
            char c = titleId[i];
            if (!((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-'))
                throw new ArgumentException("Title ID contains an invalid character.", "titleId");
        }
        if (titleId == "." || titleId == "..")
            throw new ArgumentException("Title ID cannot be a dot segment.", "titleId");
        return titleId.ToUpperInvariant();
    }

    public static string SanitizeName(string name, int maximumUtf8Bytes)
    {
        if (String.IsNullOrWhiteSpace(name) || maximumUtf8Bytes < 1)
            throw new ArgumentException("Package name is invalid.", "name");
        StringBuilder value = new StringBuilder();
        bool lastReplacement = false;
        foreach (char c in name.Normalize(NormalizationForm.FormC))
        {
            bool invalid = Char.IsControl(c) || c == '/' || c == '\\' || c == ':' || c == '*' || c == '?' || c == '"' || c == '<' || c == '>' || c == '|';
            if (invalid)
            {
                if (!lastReplacement) value.Append('-');
                lastReplacement = true;
            }
            else
            {
                value.Append(c);
                lastReplacement = false;
            }
        }
        string result = value.ToString().Trim().Trim('.');
        result = TruncateUtf8(result, maximumUtf8Bytes).Trim().Trim('.');
        if (result.Length == 0 || result == "." || result == ".." || IsReplacementOnly(result))
            throw new ArgumentException("Package name has no usable characters.", "name");
        return result;
    }

    public static PackagePaths BuildPackagePaths(string root, bool consoleMode, string titleId, string version, string name)
    {
        string canonicalRoot = NormalizeDownloadRoot(root, consoleMode);
        string title = NormalizeTitleId(titleId);
        string normalizedVersion = NormalizeVersion(version);
        string prefix = title + "_" + normalizedVersion + "_";
        int available = MaximumFileNameUtf8Bytes - Encoding.UTF8.GetByteCount(prefix) - Encoding.UTF8.GetByteCount(".pkg.resume");
        if (available < 1)
            throw new ArgumentException("Title and version leave no room for a package name.");
        string fileName = prefix + SanitizeName(name, available) + ".pkg";
        string finalPath = consoleMode ? canonicalRoot + fileName : Path.Combine(canonicalRoot, fileName);
        return new PackagePaths(canonicalRoot, finalPath, finalPath + ".resume");
    }

    public static Uri NormalizeDownloadUrl(string source)
    {
        Uri uri;
        if (!Uri.TryCreate(source, UriKind.Absolute, out uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("An absolute HTTP or HTTPS URL is required.", "source");
        if (!String.IsNullOrEmpty(uri.UserInfo) || !String.IsNullOrEmpty(uri.Fragment) || String.IsNullOrEmpty(uri.Host))
            throw new ArgumentException("URL user information and fragments are not allowed.", "source");
        return new Uri(uri.GetComponents(UriComponents.SchemeAndServer | UriComponents.PathAndQuery, UriFormat.UriEscaped), UriKind.Absolute);
    }

    public static string CreateStableQueueId(string source, string titleId, string version)
    {
        string material = NormalizeDownloadUrl(source).AbsoluteUri + "\n" + NormalizeTitleId(titleId) + "\n" + NormalizeVersion(version);
        using (SHA256 hash = SHA256.Create())
            return ToHex(hash.ComputeHash(Encoding.UTF8.GetBytes(material)));
    }

    public static ulong ParseExpectedSize(string value)
    {
        ulong size;
        if (String.IsNullOrEmpty(value) || !UInt64.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out size) || size == 0)
            throw new ArgumentException("Expected size must be a nonzero unsigned byte count.", "value");
        return size;
    }

    public static string NormalizeSha256(string value)
    {
        if (String.IsNullOrWhiteSpace(value)) return null;
        if (value.Length != 64) throw new ArgumentException("SHA-256 must contain exactly 64 hexadecimal characters.", "value");
        for (int i = 0; i < value.Length; i++)
            if (!IsHex(value[i])) throw new ArgumentException("SHA-256 contains a non-hexadecimal character.", "value");
        return value.ToLowerInvariant();
    }

    public static PackageVerification VerifyPackage(string path, ulong expectedSize, string expectedSha256)
    {
        string normalizedHash = NormalizeSha256(expectedSha256);
        if (!File.Exists(path)) return new PackageVerification(false, "File does not exist.", 0, null);
        try
        {
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                ulong length = (ulong)stream.Length;
                if (length != expectedSize) return new PackageVerification(false, "File size does not match.", length, null);
                byte[] magic = new byte[4];
                if (stream.Read(magic, 0, magic.Length) != magic.Length || magic[0] != 0x7F || magic[1] != 0x43 || magic[2] != 0x4E || magic[3] != 0x54)
                    return new PackageVerification(false, "PKG magic does not match.", length, null);
                string actualHash = null;
                if (normalizedHash != null)
                {
                    stream.Position = 0;
                    using (SHA256 hash = SHA256.Create()) actualHash = ToHex(hash.ComputeHash(stream));
                    if (!String.Equals(actualHash, normalizedHash, StringComparison.Ordinal))
                        return new PackageVerification(false, "SHA-256 does not match.", length, actualHash);
                }
                return new PackageVerification(true, null, length, actualHash);
            }
        }
        catch (Exception ex)
        {
            return new PackageVerification(false, ex.Message, 0, null);
        }
    }

    public static DownloadProgress ReduceProgress(ulong previousBytes, ulong currentBytes, ulong totalBytes, ulong speedBytesPerSecond)
    {
        ulong delta = currentBytes >= previousBytes ? currentBytes - previousBytes : currentBytes;
        bool known = totalBytes != 0;
        ulong remaining = known && currentBytes < totalBytes ? totalBytes - currentBytes : 0;
        double percent = known ? Math.Min(100.0, (double)currentBytes / (double)totalBytes * 100.0) : 0.0;
        return new DownloadProgress(currentBytes, totalBytes, delta, remaining, speedBytesPerSecond, percent, known);
    }

    public static bool ShouldRetry(DownloadFailureKind kind, int httpStatus)
    {
        if (kind == DownloadFailureKind.Transport) return true;
        if (kind != DownloadFailureKind.Http) return false;
        return httpStatus == 408 || httpStatus == 425 || httpStatus == 429 || httpStatus == 500 || httpStatus == 502 || httpStatus == 503 || httpStatus == 504;
    }

    public static int GetRetryDelaySeconds(int attempt, int baseDelaySeconds)
    {
        if (attempt < 0) throw new ArgumentOutOfRangeException("attempt");
        if (baseDelaySeconds < 0) throw new ArgumentOutOfRangeException("baseDelaySeconds");
        long delay = baseDelaySeconds;
        for (int i = 0; i < attempt && delay < MaximumRetryDelaySeconds; i++) delay = Math.Min((long)MaximumRetryDelaySeconds, delay * 2L);
        return (int)Math.Min((long)MaximumRetryDelaySeconds, delay);
    }

    private static string NormalizeVersion(string version)
    {
        if (String.IsNullOrWhiteSpace(version)) return "unknown";
        return NormalizeTitleId(version);
    }

    private static bool IsReplacementOnly(string value)
    {
        for (int i = 0; i < value.Length; i++)
            if (value[i] != '-' && Char.IsWhiteSpace(value[i]) == false)
                return false;
        return true;
    }

    private static bool IsNumberedMount(string value, string prefix)
    {
        if (!value.StartsWith(prefix, StringComparison.Ordinal) || value.Length == prefix.Length) return false;
        for (int i = prefix.Length; i < value.Length; i++) if (value[i] < '0' || value[i] > '9') return false;
        return true;
    }

    private static bool HasDotSegment(string value)
    {
        string[] parts = value.Split('/');
        for (int i = 0; i < parts.Length; i++) if (parts[i] == "." || parts[i] == "..") return true;
        return false;
    }

    private static bool ContainsControl(string value)
    {
        for (int i = 0; i < value.Length; i++) if (Char.IsControl(value[i])) return true;
        return false;
    }

    private static string TruncateUtf8(string value, int maximumBytes)
    {
        StringBuilder result = new StringBuilder();
        int bytes = 0;
        for (int i = 0; i < value.Length; i++)
        {
            int chars = Char.IsHighSurrogate(value[i]) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1]) ? 2 : 1;
            string element = value.Substring(i, chars);
            int elementBytes = Encoding.UTF8.GetByteCount(element);
            if (bytes + elementBytes > maximumBytes) break;
            result.Append(element); bytes += elementBytes; i += chars - 1;
        }
        return result.ToString();
    }

    private static bool IsHex(char c)
    {
        return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }

    private static string ToHex(byte[] bytes)
    {
        StringBuilder value = new StringBuilder(bytes.Length * 2);
        for (int i = 0; i < bytes.Length; i++) value.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return value.ToString();
    }
}
