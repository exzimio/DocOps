using DocOps.Domain.Documents;
using DocOps.Domain.Workflows;
using DocOps.Infrastructure.Documents;
using DocOps.Infrastructure.Persistence;

namespace DocOps.Infrastructure.Workflows;

/// <summary>
/// Which process instances someone may see, shared by the instance list and the task centre.
///
/// An instance or a task carries its document's reference and title, so a permission to see
/// workflows or all tasks would otherwise reveal restricted documents. An instance about a document
/// is visible to whoever may see that document, through the same predicate as the document list,
/// and to the people taking part in it: whoever started it and whoever holds or held one of its
/// tasks, directly, through a department or role, or before it was reassigned. They already know
/// the document from their own work.
/// </summary>
public static class WorkflowVisibility
{
    public static IQueryable<WorkflowInstance> VisibleInstances(
        DocOpsDbContext dbContext, AccessSubject subject, DateTimeOffset now)
    {
        IQueryable<Guid> visibleDocumentIds = dbContext.Documents
            .Where(DocumentAccessResolver.BuildVisibilityPredicate(subject, now))
            .Select(d => d.Id);

        Guid userId = subject.UserId;
        Guid[] departmentIds = [.. subject.DepartmentIds];
        string[] roles = [.. subject.Roles];

        return dbContext.WorkflowInstances.Where(i =>
            i.DocumentId == null
            || visibleDocumentIds.Contains(i.DocumentId.Value)
            || i.StartedByUserId == userId
            || i.Tasks.Any(t =>
                t.AssignedToUserId == userId
                || t.CompletedByUserId == userId
                || t.ReassignedFromUserId == userId
                || (t.AssignedToDepartmentId != null && departmentIds.Contains(t.AssignedToDepartmentId.Value))
                || (t.AssignedToRole != null && roles.Contains(t.AssignedToRole))));
    }
}
