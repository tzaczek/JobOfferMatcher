using System.Security.Cryptography;

namespace JobOfferMatcher.Application.Backup;

/// <summary>
/// Shared SHA-256 helper so the manifest's CV file hashes (written by <c>BackupService</c>) and the
/// restore-time verification (in <c>ZipBackupArchiveStore</c>) agree byte-for-byte (003 data-model §2).
/// Hex, lowercase.
/// </summary>
public static class BackupHashing
{
    public static string Sha256Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
