using DocOps.Application.Abstractions;
using DocOps.Application.Documents;
using DocOps.Application.Integrations;
using DocOps.Domain.Common;
using DocOps.Domain.Documents;
using DocOps.Domain.Integrations;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DocOps.Infrastructure.Integrations;

/// <summary>
/// Imports whatever has been dropped into a watched folder.
///
/// Nothing here is simulated: the folder is a real folder, the files are real files, and each one
/// becomes a document through <see cref="IDocumentService"/> — same validation, same reference, same
/// permissions, same audit entry as a manual upload.
///
/// It only runs when a **person** asks for it, and that is a decision rather than a limitation. A
/// document has an owner, and the worker has no user. The alternatives were to invent a system
/// account — which would put a real person's name nowhere and a fictional one on every imported
/// filing — or to attribute the import to whichever administrator happened to be seeded first. Both
/// are worse than saying plainly that this one needs somebody to press the button.
///
/// Importing is idempotent by moving: a file that becomes a document is moved out of the watched
/// folder into <c>importados/</c>, so a second run has nothing left to import rather than making a
/// second copy.
/// </summary>
public sealed class FileImportIntegrationAdapter(
    DocOpsDbContext dbContext,
    IDocumentService documentService,
    IOptions<FileStorageOptions> storageOptions,
    IHostEnvironment environment,
    ILogger<FileImportIntegrationAdapter> logger) : IIntegrationAdapter
{
    /// <summary>Where the imported originals are moved to, inside the watched folder.</summary>
    private const string ProcessedFolder = "importados";

    private readonly FileStorageOptions _storage = storageOptions.Value;

    public IntegrationAdapterInfo Info { get; } = new(
        IntegrationKind.FileImport,
        "Importação de ficheiros",
        "Importa para o repositório os ficheiros colocados numa pasta vigiada, como documentos por classificar.",
        RequiresCredentials: false,
        IsSimulated: false);

    public async Task<IntegrationRunResult> RunAsync(
        IntegrationRunContext context, CancellationToken cancellationToken = default)
    {
        string folder = WatchedFolder(context.TenantId);

        if (context.ActingUserId is null)
        {
            // Said out loud rather than failed silently: the card explains why the schedule skips it.
            return IntegrationRunResult.Skipped(
                "A importação precisa de um utilizador: um documento tem dono e o trabalho agendado "
                + "não tem sessão. Execute a partir do ecrã de integrações.",
                IntegrationStatus.Connected);
        }

        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, ProcessedFolder));

        string[] files = [.. Directory
            .EnumerateFiles(folder, "*", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.Ordinal)];

        if (files.Length == 0)
        {
            return IntegrationRunResult.Skipped(
                $"Nada por importar em «{Display(folder)}».", IntegrationStatus.Connected);
        }

        Guid? intakeTypeId = await IntakeTypeIdAsync(context.TenantId, cancellationToken);

        if (intakeTypeId is null)
        {
            return new IntegrationRunResult
            {
                Outcome = IntegrationRunOutcome.Failed,
                Summary = "O tipo «Por classificar» não existe nesta organização.",
                Status = IntegrationStatus.Failing,
                Entries =
                [
                    new IntegrationLogEntry(
                        IntegrationLogLevel.Error,
                        "import",
                        "O tipo de documento «Por classificar» não existe, e é ele que recebe o que ainda ninguém leu.")
                ]
            };
        }

        List<IntegrationLogEntry> entries = [];
        int imported = 0;
        int failed = 0;

        foreach (string path in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                string reference = await ImportAsync(path, intakeTypeId.Value, cancellationToken);
                Archive(path, folder);
                imported++;

                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Information,
                    "import",
                    $"«{Path.GetFileName(path)}» importado como {reference}.",
                    RecordsProcessed: 1));
            }
            catch (Exception exception) when (exception is DomainException or IOException)
            {
                // One file that cannot be imported must not stop the rest — and the reason belongs
                // in the log where an operator will look for it, not only in the server's own logs.
                failed++;
                entries.Add(new IntegrationLogEntry(
                    IntegrationLogLevel.Error,
                    "import",
                    $"«{Path.GetFileName(path)}» não foi importado.",
                    exception.Message));

                logger.LogWarning(exception, "Falha ao importar {File}", Path.GetFileName(path));
            }
        }

        return new IntegrationRunResult
        {
            Outcome = failed == 0
                ? IntegrationRunOutcome.Succeeded
                : imported > 0
                    ? IntegrationRunOutcome.PartiallyFailed
                    : IntegrationRunOutcome.Failed,
            RecordsProcessed = imported,
            Summary = failed == 0
                ? $"{imported} ficheiro(s) importado(s) de «{Display(folder)}»."
                : $"{imported} ficheiro(s) importado(s), {failed} com erro.",
            Entries = entries
        };
    }

    private async Task<string> ImportAsync(string path, Guid documentTypeId, CancellationToken cancellationToken)
    {
        await using FileStream content = File.OpenRead(path);

        DocumentDetailDto document = await documentService.CreateAsync(
            new CreateDocumentRequest
            {
                Title = Path.GetFileNameWithoutExtension(path),
                DocumentTypeId = documentTypeId,
                Description = $"Importado da pasta vigiada em {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC.",
                Tags = ["importado"]
            },
            new DocumentUpload(content, Path.GetFileName(path), ContentType(path), content.Length),
            cancellationToken);

        return document.Summary.Reference;
    }

    /// <summary>
    /// Moves the original out of the watched folder. Keeping it would import it again on the next
    /// run; deleting it would throw away the only copy of something that just failed validation
    /// somewhere downstream.
    /// </summary>
    private static void Archive(string path, string folder)
    {
        string destination = Path.Combine(folder, ProcessedFolder, Path.GetFileName(path));

        if (File.Exists(destination))
        {
            string stem = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            destination = Path.Combine(
                folder, ProcessedFolder, $"{stem}-{DateTime.UtcNow:yyyyMMddHHmmss}{extension}");
        }

        File.Move(path, destination);
    }

    private Task<Guid?> IntakeTypeIdAsync(Guid tenantId, CancellationToken cancellationToken) =>
        dbContext.DocumentTypes
            .IgnoreQueryFilters()
            .Where(type => type.TenantId == tenantId && type.Code == DocumentTypeCodes.Unclassified)
            .Select(type => (Guid?)type.Id)
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>One watched folder per organisation: a shared one would be a way into another's repository.</summary>
    private string WatchedFolder(Guid tenantId)
    {
        string root = Path.IsPathRooted(_storage.RootPath)
            ? _storage.RootPath
            : Path.Combine(environment.ContentRootPath, _storage.RootPath);

        return Path.Combine(root, "import", tenantId.ToString("N"));
    }

    private static string Display(string folder) => folder.Replace('\\', '/');

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        _ => "application/octet-stream"
    };
}
