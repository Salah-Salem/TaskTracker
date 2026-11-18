using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Common;
using TaskTracker.Application.Tasks;
using TaskTracker.Domain.Entities;
using TaskTracker.Domain.Enums;
using TaskTracker.Infrastructure.Common;
using TaskTracker.Infrastructure.Persistence;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Infrastructure.Tasks;

public class TaskService(AppDbContext db, TimeProvider clock, ILogger<TaskService> log) : ITaskService
{
    public const string TaskNotFound = "Task not found";
    public const string AssigneeNotMember = "Assignee must be a member of the project";
    public const string AssigneeIsViewer = "Assignee must be a project Owner or Member (Viewers cannot be assigned)";

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    private static readonly Expression<Func<TaskItem, TaskDto>> ToDto = t => new TaskDto(
        t.Id, t.ProjectId, t.Project.Name, t.Title, t.Description, t.Status, t.Priority,
        t.AssigneeId, t.Assignee != null ? t.Assignee.DisplayName : null,
        t.DueDate, t.CreatedById, t.CreatedAt, t.UpdatedAt, t.CompletedAt);

    public Task<PagedResult<TaskDto>> ListAsync(ProjectAccess access, TaskListQuery query)
    {
        var q = db.Tasks.AsNoTracking().Where(t => t.ProjectId == access.ProjectId);

        if (query.AssigneeId is { } a)
        {
            if (a.Equals("none", StringComparison.OrdinalIgnoreCase))
                q = q.Where(t => t.AssigneeId == null);
            else
            {
                var assigneeId = Guid.Parse(a); // format checked by TaskListQueryValidator
                q = q.Where(t => t.AssigneeId == assigneeId);
            }
        }

        return Sort(ApplyFilters(q, query), query).Select(ToDto).ToPagedAsync(query);
    }

    public Task<PagedResult<TaskDto>> ListMineAsync(CurrentUser user, TaskFilterQuery query)
    {
        // Assigned to me, and only in projects I still belong to.
        var q = db.Tasks.AsNoTracking()
            .Where(t => t.AssigneeId == user.Id && t.Project.Members.Any(m => m.UserId == user.Id));

        return Sort(ApplyFilters(q, query), query).Select(ToDto).ToPagedAsync(query);
    }

    public Task<TaskDto?> GetAsync(ProjectAccess access, Guid taskId) =>
        db.Tasks.AsNoTracking()
            .Where(t => t.Id == taskId && t.ProjectId == access.ProjectId)
            .Select(ToDto)
            .SingleOrDefaultAsync();

    public async Task<ServiceResult<TaskDto>> CreateAsync(ProjectAccess access, CurrentUser user, CreateTaskRequest req)
    {
        if (await CheckAssigneeAsync(access.ProjectId, req.AssigneeId) is { } error)
            return error;

        var now = Now;
        var task = new TaskItem
        {
            Id = Guid.NewGuid(),
            ProjectId = access.ProjectId,
            Title = req.Title.Trim(),
            Description = req.Description?.Trim(),
            Priority = req.Priority ?? TaskPriority.Medium,
            AssigneeId = req.AssigneeId,
            DueDate = req.DueDate,
            CreatedById = user.Id,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.Tasks.Add(task);
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} created task {TaskId} in project {ProjectId}", user.Id, task.Id, access.ProjectId);
        return ServiceResult<TaskDto>.Ok((await GetAsync(access, task.Id))!);
    }

    public async Task<ServiceResult<TaskDto>> UpdateAsync(ProjectAccess access, CurrentUser user, Guid taskId, UpdateTaskRequest req)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == access.ProjectId);
        if (task is null)
            return ServiceResult<TaskDto>.NotFound(TaskNotFound);

        if (req.AssigneeId.IsSet && await CheckAssigneeAsync(access.ProjectId, req.AssigneeId.Value) is { } error)
            return error;

        var now = Now;
        if (req.Title.IsSet) task.Title = req.Title.Value.Trim();
        if (req.Description.IsSet) task.Description = req.Description.Value?.Trim();
        if (req.Status.IsSet) task.SetStatus(req.Status.Value, now);
        if (req.Priority.IsSet) task.Priority = req.Priority.Value;
        if (req.AssigneeId.IsSet) task.AssigneeId = req.AssigneeId.Value;
        if (req.DueDate.IsSet) task.DueDate = req.DueDate.Value;
        task.UpdatedAt = now;
        task.UpdatedById = user.Id;
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} updated task {TaskId}", user.Id, task.Id);
        return ServiceResult<TaskDto>.Ok((await GetAsync(access, task.Id))!);
    }

    public async Task<bool> DeleteAsync(ProjectAccess access, CurrentUser user, Guid taskId)
    {
        var task = await db.Tasks.SingleOrDefaultAsync(t => t.Id == taskId && t.ProjectId == access.ProjectId);
        if (task is null) return false; // includes "already deleted" → second DELETE is a 404

        task.IsDeleted = true; // soft delete; comments/time logs disappear with it via query filters
        task.UpdatedAt = Now;
        task.UpdatedById = user.Id;
        await db.SaveChangesAsync();

        log.LogInformation("Audit: user {UserId} deleted task {TaskId}", user.Id, task.Id);
        return true;
    }

    /// <summary>Null when OK. Not a member → 400; a Viewer → 422 (business rule).</summary>
    private async Task<ServiceResult<TaskDto>?> CheckAssigneeAsync(Guid projectId, Guid? assigneeId)
    {
        if (assigneeId is null) return null;

        var role = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && m.UserId == assigneeId)
            .Select(m => (ProjectRole?)m.Role)
            .SingleOrDefaultAsync();

        return role switch
        {
            null => ServiceResult<TaskDto>.BadRequest(AssigneeNotMember),
            ProjectRole.Viewer => ServiceResult<TaskDto>.Unprocessable(AssigneeIsViewer),
            _ => null,
        };
    }

    private static IQueryable<TaskItem> ApplyFilters(IQueryable<TaskItem> q, TaskFilterQuery f)
    {
        if (!string.IsNullOrWhiteSpace(f.Search))
        {
            // Parameterised LIKE with escaped wildcards — never string-concatenated SQL.
            // SQLite's LIKE is case-insensitive for ASCII, which covers the POC's needs.
            var like = QueryRules.LikeContains(f.Search);
            q = q.Where(t => EF.Functions.Like(t.Title, like, "\\")
                          || (t.Description != null && EF.Functions.Like(t.Description, like, "\\")));
        }

        // Formats were checked by the validator, so parsing cannot fail here.
        QueryRules.TryParseEnums<TaskStatus>(f.Status, out var statuses);
        if (statuses.Count > 0) q = q.Where(t => statuses.Contains(t.Status));

        QueryRules.TryParseEnums<TaskPriority>(f.Priority, out var priorities);
        if (priorities.Count > 0) q = q.Where(t => priorities.Contains(t.Priority));

        if (f.DueAfter is { } after) q = q.Where(t => t.DueDate >= after);
        if (f.DueBefore is { } before) q = q.Where(t => t.DueDate <= before);

        return q;
    }

    private static IOrderedQueryable<TaskItem> Sort(IQueryable<TaskItem> q, TaskFilterQuery f)
    {
        var desc = f.Descending;
        var sorted = f.SortBy?.ToLowerInvariant() switch
        {
            "title" => q.OrderByDir(t => t.Title.ToLower(), desc),
            "status" => q.OrderByDir(t => t.Status, desc),
            "priority" => q.OrderByDir(t => t.Priority, desc),
            "duedate" => q.OrderByDir(t => t.DueDate, desc),
            "updatedat" => q.OrderByDir(t => t.UpdatedAt, desc),
            _ => q.OrderByDir(t => t.CreatedAt, desc),
        };
        return sorted.ThenBy(t => t.Id); // stable paging
    }
}
