using FluentValidation;
using TaskTracker.Application.Common;

namespace TaskTracker.Application.Activity;

// ---------- Comments ----------

public record CommentDto(Guid Id, Guid TaskId, Guid AuthorId, string AuthorName, string Body, DateTime CreatedAt, DateTime UpdatedAt);

public record CommentRequest(string Body);

public class CommentRequestValidator : AbstractValidator<CommentRequest>
{
    public CommentRequestValidator() => RuleFor(x => x.Body).NotEmpty().MaximumLength(2000);
}

public interface ICommentService
{
    /// <summary>Null when the task does not exist in this project.</summary>
    Task<PagedResult<CommentDto>?> ListAsync(ProjectAccess access, Guid taskId, PageQuery query);
    Task<CommentDto?> CreateAsync(ProjectAccess access, CurrentUser user, Guid taskId, CommentRequest request);

    /// <summary>Loads the comment (scoped to project + task) for the author-or-owner check. Null → 404.</summary>
    Task<AuthoredResource?> FindAsync(ProjectAccess access, Guid taskId, Guid commentId);
    Task<CommentDto> UpdateAsync(CurrentUser user, Guid commentId, CommentRequest request);
    Task DeleteAsync(CurrentUser user, Guid commentId);
}

// ---------- Time logs ----------

public record TimeLogDto(Guid Id, Guid TaskId, Guid UserId, string UserName, int Minutes, DateOnly WorkDate, string? Note, DateTime CreatedAt);

/// <summary>The user is always the caller; there is deliberately no userId field.</summary>
public record CreateTimeLogRequest(int Minutes, DateOnly? WorkDate, string? Note);

/// <summary>PATCH: only fields present are applied. "note": null clears it.</summary>
public record UpdateTimeLogRequest(Optional<int> Minutes, Optional<DateOnly> WorkDate, Optional<string?> Note);

public class CreateTimeLogRequestValidator : AbstractValidator<CreateTimeLogRequest>
{
    public CreateTimeLogRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Minutes).InclusiveBetween(1, 1440);
        RuleFor(x => x.WorkDate).Must(d => d is null || d <= Today(clock)).WithMessage("workDate cannot be in the future");
        RuleFor(x => x.Note).MaximumLength(500);
    }

    internal static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
}

public class UpdateTimeLogRequestValidator : AbstractValidator<UpdateTimeLogRequest>
{
    public UpdateTimeLogRequestValidator(TimeProvider clock)
    {
        RuleFor(x => x.Minutes.Value).InclusiveBetween(1, 1440).OverridePropertyName("Minutes").When(x => x.Minutes.IsSet);
        RuleFor(x => x.WorkDate.Value).Must(d => d <= CreateTimeLogRequestValidator.Today(clock))
            .OverridePropertyName("WorkDate").WithMessage("workDate cannot be in the future").When(x => x.WorkDate.IsSet);
        RuleFor(x => x.Note.Value).MaximumLength(500).OverridePropertyName("Note").When(x => x.Note.IsSet);
    }
}

public interface ITimeLogService
{
    Task<PagedResult<TimeLogDto>?> ListAsync(ProjectAccess access, Guid taskId, PageQuery query);
    Task<TimeLogDto?> CreateAsync(ProjectAccess access, CurrentUser user, Guid taskId, CreateTimeLogRequest request);

    Task<AuthoredResource?> FindAsync(ProjectAccess access, Guid taskId, Guid logId);
    Task<TimeLogDto> UpdateAsync(CurrentUser user, Guid logId, UpdateTimeLogRequest request);
    Task DeleteAsync(CurrentUser user, Guid logId);
}
