using DocOps.Domain.Documents;
using DocOps.Domain.Notifications;
using DocOps.Infrastructure.Compliance;
using DocOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using DocOps.Application.Abstractions;

namespace DocOps.IntegrationTests;

/// <summary>
/// A verificação de retenção contra SQL Server real.
///
/// O que é preciso provar aqui não é que o aviso aparece — é que **nada desaparece**. Um prazo de
/// conservação que termina significa que a organização passa a poder eliminar o documento, e não
/// que um trabalho agendado o deva fazer de madrugada sem ninguém a ver. A base legal que obrigou a
/// guardá-lo é a mesma que faz da destruição uma decisão que alguém assina.
/// </summary>
[Collection(ApiCollection.Name)]
public class RetentionCheckTests(DocOpsApiFactory factory)
{
    [Fact]
    public async Task A_document_past_its_retention_is_announced_and_left_where_it_is()
    {
        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Document document = await GivenAnExpiredDocumentAsync(context);

        int handled = await RunJobAsync(scope);

        Assert.True(handled > 0);

        // O documento continua lá, inteiro: nem eliminado, nem marcado como eliminado.
        Document after = await context.Documents
            .IgnoreQueryFilters()
            .FirstAsync(d => d.Id == document.Id);

        Assert.False(after.IsDeleted);
        Assert.Equal(document.Status, after.Status);

        // E alguém foi avisado, com o documento à mão em vez de uma frase genérica.
        Notification notice = await context.Notifications
            .IgnoreQueryFilters()
            .FirstAsync(n => n.DocumentId == document.Id && n.Kind == NotificationKind.RetentionDue);

        Assert.Contains(document.Reference, notice.Title, StringComparison.Ordinal);
        Assert.Equal($"/app/documents/{document.Id}", notice.Link);
    }

    [Fact]
    public async Task The_same_document_is_not_announced_twice()
    {
        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Document document = await GivenAnExpiredDocumentAsync(context);

        await RunJobAsync(scope);

        int first = await context.Notifications
            .IgnoreQueryFilters()
            .CountAsync(n => n.DocumentId == document.Id && n.Kind == NotificationKind.RetentionDue);

        await RunJobAsync(scope);

        int second = await context.Notifications
            .IgnoreQueryFilters()
            .CountAsync(n => n.DocumentId == document.Id && n.Kind == NotificationKind.RetentionDue);

        // O worker passa a cada poucos minutos. Um aviso que se repetisse em cada passagem deixaria
        // de ser lido dentro de uma hora, e o documento ficaria por eliminar na mesma.
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task A_document_still_inside_its_period_is_left_alone()
    {
        using ISystemScope scope = factory.Services.GetRequiredService<ISystemScopeFactory>().Create();
        DocOpsDbContext context = scope.Services.GetRequiredService<DocOpsDbContext>();

        Document document = await GivenAnExpiredDocumentAsync(context, expired: false);

        await RunJobAsync(scope);

        bool announced = await context.Notifications
            .IgnoreQueryFilters()
            .AnyAsync(n => n.DocumentId == document.Id && n.Kind == NotificationKind.RetentionDue);

        Assert.False(announced);
    }

    private static async Task<int> RunJobAsync(ISystemScope scope) =>
        await scope.Services
            .GetRequiredService<RetentionChecker>()
            .AnnounceExpiredAsync(CancellationToken.None);

    /// <summary>
    /// Um documento arquivado com a data de conservação no passado — ou no futuro, para o caso
    /// contrário. Escrito diretamente na base: o que está sob teste é a verificação, não o percurso
    /// que levou o documento até ali.
    /// </summary>
    private static async Task<Document> GivenAnExpiredDocumentAsync(
        DocOpsDbContext context, bool expired = true)
    {
        Document template = await context.Documents.IgnoreQueryFilters().FirstAsync();

        Document document = new()
        {
            Id = Guid.CreateVersion7(),
            TenantId = template.TenantId,
            Title = $"Documento de retenção {Guid.NewGuid():N}"[..40],
            Reference = $"RET-{Guid.NewGuid():N}"[..14].ToUpperInvariant(),
            DocumentTypeId = template.DocumentTypeId,
            OwnerUserId = template.OwnerUserId,
            Status = DocumentStatus.Archived,
            CreatedAtUtc = DateTimeOffset.UtcNow.AddYears(-11),
            RetentionDueAtUtc = expired
                ? DateTimeOffset.UtcNow.AddDays(-1)
                : DateTimeOffset.UtcNow.AddYears(5)
        };

        context.Documents.Add(document);
        await context.SaveChangesAsync();

        return document;
    }
}
