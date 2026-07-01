using JobOfferMatcher.Application.Applications;
using JobOfferMatcher.Domain.Common.Ids;
using Microsoft.Extensions.Configuration;

namespace JobOfferMatcher.Infrastructure.Applications;

/// <summary>
/// Stores application attachments as flat files in the SAME <c>cv-data</c> root the CV / tailored-CV
/// stores use (data-model §6, ADR-4). Files are named <c>appdoc-{ApplicationDocumentId:N}{ext}</c> — a
/// distinct prefix from <c>{cvId}</c> / <c>tailored-</c> so there are no collisions — so 003's top-level
/// <c>EnumerateAll()</c> + atomic <c>StageSwap</c> back them up and restore them with zero changes.
/// Singleton (stateless, like <c>LocalCvFileStore</c> / <c>LocalTailoredCvFileStore</c>).
/// </summary>
public sealed class LocalApplicationDocumentFileStore : IApplicationDocumentFileStore
{
    private readonly string _directory;

    public LocalApplicationDocumentFileStore(IConfiguration configuration)
    {
        _directory = configuration["Cv:StoragePath"] ?? Path.Combine(AppContext.BaseDirectory, "cv-data");
        Directory.CreateDirectory(_directory);
    }

    public async Task<string> SaveAsync(ApplicationDocumentId id, string originalFileName, byte[] content, CancellationToken ct = default)
    {
        var storedName = StoredName(id, originalFileName);
        await File.WriteAllBytesAsync(Path.Combine(_directory, storedName), content, ct);
        return storedName;
    }

    public string GetAbsolutePath(string storedFileName) =>
        Path.GetFullPath(Path.Combine(_directory, storedFileName));

    public void Delete(string storedFileName)
    {
        var path = Path.Combine(_directory, storedFileName);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>Flat name <c>appdoc-{id:N}{ext}</c>; the extension is carried over from the original name.</summary>
    private static string StoredName(ApplicationDocumentId id, string originalFileName)
    {
        var ext = Path.GetExtension(originalFileName);
        // Guard against path separators / odd input: only keep a short, safe extension.
        if (ext.Length is 0 or > 20 || ext.Any(c => !(char.IsLetterOrDigit(c) || c == '.')))
        {
            ext = string.Empty;
        }

        return $"appdoc-{id.Value:N}{ext}";
    }
}
