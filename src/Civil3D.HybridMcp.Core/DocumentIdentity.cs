using System.Security.Cryptography;
using System.Text;

namespace Civil3D.HybridMcp.Core;

public static class DocumentIdentity
{
    public static string ComputeDocumentId(
        string instanceId,
        string name,
        string? fullPath,
        string sessionIdentifier)
    {
        var normalizedPath = string.IsNullOrWhiteSpace(fullPath)
            ? "<unsaved>"
            : Path.GetFullPath(fullPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToUpperInvariant();

        var payload = string.Join("|", instanceId, normalizedPath, name, sessionIdentifier);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }
}

