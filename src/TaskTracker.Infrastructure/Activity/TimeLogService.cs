using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Common;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Common;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Infrastructure.Activity;

public class TimeLogService(AppDbContext db, TimeProvider clock, ILogger<TimeLogService> log) : ITimeLogService
{
    private static readonly Expression<Func<TimeLog, TimeLogDto>> ToDto = l =>
        new TimeLogDto(l.Id, l.TaskId, l.UserId, l.User.DisplayName, l.Minutes, l.WorkDate, l.Note, l.CreatedAt);

    public async Task<PagedResult<TimeLogDto>?> ListAsync(ProjectAccess access, Guid taskId, PageQuery query)
    {
        if (!await TaskExistsAsync(access, taskId)) return null;

        return await db.TimeLogs.AsNoTracking()
            .Where(l => l.TaskId == taskId)
            .OrderByDescending(l => l.CreatedAt).ThenByDescending(l => l.Id) // newest first
            .Select(ToDto)
            .ToPagedAsync(query);
    }

    public async Task<TimeLogDto?> CreateAsync(ProjectAccess access, CurrentUser user, Guid taskId, CreateTimeLogRequest req)
    {
        if (!await TaskExistsAsync(access, taskId)) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var entry = new TimeLog
        {
            Id = Guid.NewGuid(),
            TaskId = taskId,
            UserId = user.Id, // always the caller (from the JWT), never from the body
            Minutes = req.Minutes,
            WorkDate = req.WorkDate ?? DateOnly.FromDateTime(now),
            Note = req.Note?.Trim(),
            CreatedAt = now,
        };
        db.TimeLogs.Add(entry);
        await db.SaveChangesAsync();

        return await db.TimeLogs.AsNoTracking().Where(l => l.Id == entry.Id).Select(ToDto).SingleAsync();
    }

    public async Task<AuthoredResource?> FindAsync(ProjectAccess access, Guid taskId, Guid logId)
    {
        var authorId = await db.TimeLogs.AsNoTracking()
            .Where(l => l.Id == logId && l.TaskId == taskId && l.Task.ProjectId == access.ProjectId)
            .Select(l => (Guid?)l.UserId)
            .SingleOrDefaultAsync();
        return authorId is null ? null : new AuthoredResource(authorId.Value, access);
    }

    public async Task<TimeLogDto> UpdateAsync(CurrentUser user, Guid logId, UpdateTimeLogRequest req)
    {
        var entry = await db.TimeLogs.SingleAsync(l => l.Id == logId);
        if (req.Minutes.IsSet) entry.Minutes = req.Minutes.Value;
        if (req.WorkDate.IsSet) entry.WorkDate = req.WorkDate.Value;
        if (req.Note.IsSet) entry.Note = req.Note.Value?.Trim();
        await db.SaveChangesAsync();

        if (entry.UserId != user.Id)
            log.LogInformation("Audit: user {UserId} edited time log {LogId} of {OwnerId} (moderation)", user.Id, logId, entry.UserId);
        return await db.TimeLogs.AsNoTracking().Where(l => l.Id == logId).Select(ToDto).SingleAsync();
    }

    public async Task DeleteAsync(CurrentUser user, Guid logId)
    {
        var entry = await db.TimeLogs.SingleAsync(l => l.Id == logId);
        db.TimeLogs.Remove(entry);
        await db.SaveChangesAsync();
        log.LogInformation("Audit: user {UserId} deleted time log {LogId} of {OwnerId}", user.Id, logId, entry.UserId);
    }

    private Task<bool> TaskExistsAsync(ProjectAccess access, Guid taskId) =>
        db.Tasks.AnyAsync(t => t.Id == taskId && t.ProjectId == access.ProjectId);
}
