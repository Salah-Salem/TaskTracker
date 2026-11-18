using FluentValidation;
using TaskTracker.Application.Common;
using TaskTracker.Domain.Enums;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Application.Tasks;

public record TaskDto(
    Guid Id, Guid ProjectId, string ProjectName,
    string Title, string? Description,
    TaskStatus Status, TaskPriority Priority,
    Guid? AssigneeId, string? AssigneeName,
    DateOnly? DueDate, Guid CreatedById,
    DateTime CreatedAt, DateTime UpdatedAt, DateTime? CompletedAt);

/// <summary>New tasks always start as Todo.</summary>
public record CreateTaskRequest(
    string Title, string? Description,
    TaskPriority? Priority, Guid? AssigneeId, DateOnly? DueDate);

/// <summary>PATCH: only fields present in the JSON are applied. "assigneeId": null unassigns.</summary>
public record UpdateTaskRequest(
    Optional<string> Title,
    Optional<string?> Description,
    Optional<TaskStatus> Status,
    Optional<TaskPriority> Priority,
    Optional<Guid?> AssigneeId,
    Optional<DateOnly?> DueDate);

/// <summary>Filters shared by a project's task list and /api/me/tasks.</summary>
public class TaskFilterQuery : SortedPageQuery
{
    public static readonly string[] SortFields = ["title", "status", "priority", "dueDate", "createdAt", "updatedAt"];

    public string? Search { get; set; }
    /// <summary>Repeatable or comma-separated: ?status=Todo&amp;status=InProgress</summary>
    public string[]? Status { get; set; }
    public string[]? Priority { get; set; }
    public DateOnly? DueBefore { get; set; }
    public DateOnly? DueAfter { get; set; }
}

/// <summary>GET /api/projects/{id}/tasks. <c>assigneeId</c> is a user id, or <c>none</c> for unassigned.</summary>
public class TaskListQuery : TaskFilterQuery
{
    public string? AssigneeId { get; set; }
}

public abstract class TaskFilterQueryValidatorBase<T> : AbstractValidator<T> where T : TaskFilterQuery
{
    protected TaskFilterQueryValidatorBase()
    {
        this.AddSortedPageRules(TaskFilterQuery.SortFields);
        RuleFor(x => x.Search).MaximumLength(200);
        RuleFor(x => x.Status).Must(s => QueryRules.TryParseEnums<TaskStatus>(s, out _))
            .WithMessage($"Invalid status value. Allowed: {string.Join(", ", Enum.GetNames<TaskStatus>())}");
        RuleFor(x => x.Priority).Must(p => QueryRules.TryParseEnums<TaskPriority>(p, out _))
            .WithMessage($"Invalid priority value. Allowed: {string.Join(", ", Enum.GetNames<TaskPriority>())}");
        RuleFor(x => x).Must(x => x.DueBefore is null || x.DueAfter is null || x.DueAfter <= x.DueBefore)
            .WithName("dueAfter").WithMessage("dueAfter must be on or before dueBefore");
    }
}

public class TaskFilterQueryValidator : TaskFilterQueryValidatorBase<TaskFilterQuery>;

public class TaskListQueryValidator : TaskFilterQueryValidatorBase<TaskListQuery>
{
    public TaskListQueryValidator()
    {
        RuleFor(x => x.AssigneeId)
            .Must(a => a is null || a.Equals("none", StringComparison.OrdinalIgnoreCase) || Guid.TryParse(a, out _))
            .WithMessage("assigneeId must be a user id or 'none'");
    }
}

public class CreateTaskRequestValidator : AbstractValidator<CreateTaskRequest>
{
    public CreateTaskRequestValidator()
    {
        RuleFor(x => x.Title).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(4000);
        RuleFor(x => x.Priority).IsInEnum();
    }
}

public class UpdateTaskRequestValidator : AbstractValidator<UpdateTaskRequest>
{
    public UpdateTaskRequestValidator()
    {
        RuleFor(x => x.Title.Value).NotEmpty().MaximumLength(200).OverridePropertyName("Title").When(x => x.Title.IsSet);
        RuleFor(x => x.Description.Value).MaximumLength(4000).OverridePropertyName("Description").When(x => x.Description.IsSet);
        RuleFor(x => x.Status.Value).IsInEnum().OverridePropertyName("Status").When(x => x.Status.IsSet);
        RuleFor(x => x.Priority.Value).IsInEnum().OverridePropertyName("Priority").When(x => x.Priority.IsSet);
    }
}

public interface ITaskService
{
    Task<PagedResult<TaskDto>> ListAsync(ProjectAccess access, TaskListQuery query);

    /// <summary>Tasks assigned to the caller, across every project they are a member of.</summary>
    Task<PagedResult<TaskDto>> ListMineAsync(CurrentUser user, TaskFilterQuery query);

    Task<TaskDto?> GetAsync(ProjectAccess access, Guid taskId);
    Task<ServiceResult<TaskDto>> CreateAsync(ProjectAccess access, CurrentUser user, CreateTaskRequest request);
    Task<ServiceResult<TaskDto>> UpdateAsync(ProjectAccess access, CurrentUser user, Guid taskId, UpdateTaskRequest request);
    Task<bool> DeleteAsync(ProjectAccess access, CurrentUser user, Guid taskId);
}
