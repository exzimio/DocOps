namespace DocOps.Application.Abstractions;

public sealed record StoredFile(string StorageKey, long SizeBytes, string ContentHash);

public sealed record FileDescriptor(string FileName, string ContentType);

/// <summary>
/// Content storage for document versions. Metadata stays in SQL Server; bytes live here (ADR-003).
/// The implementation owns the storage key: callers never choose where a file lands, so a crafted
/// file name cannot escape the storage root.
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores the stream and returns its key, size and SHA-256 hash.</summary>
    Task<StoredFile> SaveAsync(
        Stream content, Guid tenantId, Guid documentId, FileDescriptor descriptor, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);

    /// <summary>Physically removes the content. Only used once a retention policy authorises disposal.</summary>
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Limits and allow-lists applied to every upload, enforced server-side.</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Root directory for the local provider. Relative paths resolve from the content root.</summary>
    public string RootPath { get; set; } = "storage";

    public long MaxFileSizeBytes { get; set; } = 25 * 1024 * 1024;

    public string[] AllowedContentTypes { get; set; } =
    [
        "application/pdf",
        "image/png",
        "image/jpeg",
        "text/plain",
        "text/csv",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        "application/msword",
        "application/vnd.ms-excel"
    ];

    public string[] AllowedExtensions { get; set; } =
        [".pdf", ".png", ".jpg", ".jpeg", ".txt", ".csv", ".docx", ".xlsx", ".pptx", ".doc", ".xls"];
}
