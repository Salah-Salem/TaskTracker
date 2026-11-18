using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Common;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Common;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Infrastructure.Activity;

public class CommentService(AppDbContext db, TimeProvider clock, ILogger<CommentService> log) : ICommentService
{
    private static readonly Expression<Func<Comment, CommentDto>> ToDto = c =>
        new CommentDto(c.Id, c.TaskId, c.AuthorId, c.Author.DisplayName, c.Body, c.CreatedAt, c.UpdatedAt);

    public async Task<PagedResult<CommentDto>?> ListAsync(ProjectAccess access, Guid taskId, PageQuery query)
    {
        if (!await TaskExistsAsync(access, taskId)) return null;

        return await db.Comments.AsNoTracking()
            .Where(c => c.TaskId == taskId)
            .OrderByDescending(c => c.CreatedAt).ThenByDescending(c => c.Id) // newest first
            .Select(ToDto)
            .ToPagedAsync(query);
    }

    public async Task<CommentDto?> CreateAsync(ProjectAccess access, CurrentUser user, Guid taskId, CommentRequest req)
    {
        if (!await TaskExistsAsync(access, taskId)) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var comment = new Comment
        {
            Id = Guid.NewGuid(), TaskId = taskId, AuthorId = user.Id,
            Body = req.Body.Trim(), CreatedAt = now, UpdatedAt = now,
        };
        db.Comments.Add(comment);
        await db.SaveChangesAsync();

        return await db.Comments.AsNoTracking().Where(c => c.Id == comment.Id).Select(ToDto).SingleAsync();
    }

    public async Task<AuthoredResource?> FindAsync(ProjectAccess access, Guid taskId, Guid commentId)
    {
        var authorId = await db.Comments.AsNoTracking()
            .Where(c => c.Id == commentId && c.TaskId == taskId && c.Task.ProjectId == access.ProjectId)
            .Select(c => (Guid?)c.AuthorId)
            .SingleOrDefaultAsync();
        return authorId is null ? null : new AuthoredResource(authorId.Value, access);
    }

    public async Task<CommentDto> UpdateAsync(CurrentUser user, Guid commentId, CommentRequest req)
    {
        var comment = await db.Comments.SingleAsync(c => c.Id == commentId);
        comment.Body = req.Body.Trim();
        comment.UpdatedAt = clock.GetUtcNow().UtcDateTime;
        await db.SaveChangesAsync();

        if (comment.AuthorId != user.Id)
            log.LogInformation("Audit: user {UserId} edited comment {CommentId} by {AuthorId} (moderation)", user.Id, commentId, comment.AuthorId);
        return await db.Comments.AsNoTracking().Where(c => c.Id == commentId).Select(ToDto).SingleAsync();
    }

    public async Task DeleteAsync(CurrentUser user, Guid commentId)
    {
        var comment = await db.Comments.SingleAsync(c => c.Id == commentId);
        db.Comments.Remove(comment);
        await db.SaveChangesAsync();
        log.LogInformation("Audit: user {UserId} deleted comment {CommentId} by {AuthorId}", user.Id, commentId, comment.AuthorId);
    }

    private Task<bool> TaskExistsAsync(ProjectAccess access, Guid taskId) =>
        db.Tasks.AnyAsync(t => t.Id == taskId && t.ProjectId == access.ProjectId);
}
