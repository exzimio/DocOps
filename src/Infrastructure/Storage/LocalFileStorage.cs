using System.Buffers;
using System.Security.Cryptography;
using DocOps.Application.Abstractions;
using DocOps.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Storage;

/// <summary>
/// Stores document content on the local file system, partitioned by tenant and document. Intended for
/// development and for single-node deployments; <c>AzureBlobStorage</c> implements the same interface
/// for cloud hosting without any change to the callers.
/// </summary>
public sealed class LocalFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly ILogger<LocalFileStorage> _logger;

    public LocalFileStorage(IOptions<FileStorageOptions> options, ILogger<LocalFileStorage> logger)
    {
        _logger = logger;
        string configured = options.Value.RootPath;

        // Relative paths resolve from the content root, so stored files survive a rebuild instead of
        // being wiped with the bin directory.
        _rootPath = Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Directory.GetCurrentDirectory(), configured);

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<StoredFile> SaveAsync(
        Stream content, Guid tenantId, Guid documentId, FileDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        // The key is built entirely from server-side values plus a sanitised extension: nothing the
        // caller supplies can influence the directory the file lands in.
        string extension = SafeExtension(descriptor.FileName);
        string relativeKey = $"{tenantId:N}/{documentId:N}/{Guid.CreateVersion7():N}{extension}";
        string absolutePath = ResolveAbsolute(relativeKey);

        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        // The hash is computed while the bytes stream past, so the file is never read back and the
        // write handle is never contended.
        using IncrementalHash hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = ArrayPool<byte>.Shared.Rent(81920);
        long size = 0;

        try
        {
            await using FileStream destination = new(absolutePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);

            int read;
            while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                hasher.AppendData(buffer, 0, read);
                size += read;
            }

            await destination.FlushAsync(cancellationToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        byte[] hash = hasher.GetHashAndReset();

        _logger.LogInformation(
            "Stored {SizeBytes} bytes for document {DocumentId} of tenant {TenantId}", size, documentId, tenantId);

        return new StoredFile(relativeKey, size, Convert.ToHexStringLower(hash));
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        string absolutePath = ResolveAbsolute(storageKey);

        if (!File.Exists(absolutePath))
        {
            throw new EntityNotFoundException("StoredFile", storageKey);
        }

        Stream stream = new FileStream(
            absolutePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 64 * 1024, useAsync: true);

        return Task.FromResult(stream);
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(ResolveAbsolute(storageKey)));

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        string absolutePath = ResolveAbsolute(storageKey);

        if (File.Exists(absolutePath))
        {
            File.Delete(absolutePath);
            _logger.LogInformation("Deleted stored file {StorageKey}", storageKey);
        }

        return Task.CompletedTask;
    }

    /// <summary>Resolves a key inside the storage root, refusing anything that escapes it.</summary>
    private string ResolveAbsolute(string storageKey)
    {
        string candidate = Path.GetFullPath(Path.Combine(_rootPath, storageKey));
        string root = Path.GetFullPath(_rootPath);

        if (!candidate.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenOperationException($"Storage key '{storageKey}' resolves outside the storage root.");
        }

        return candidate;
    }

    private static string SafeExtension(string fileName)
    {
        string extension = Path.GetExtension(fileName);

        if (extension.Length is 0 or > 10 || extension.Any(c => !char.IsLetterOrDigit(c) && c != '.'))
        {
            return ".bin";
        }

        return extension.ToLowerInvariant();
    }
}
