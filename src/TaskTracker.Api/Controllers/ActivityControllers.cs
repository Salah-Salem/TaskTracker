using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Api.Auth;
using TaskTracker.Application.Activity;
using TaskTracker.Application.Common;

namespace TaskTracker.Api.Controllers;

/// <summary>
/// Comments on a task. List: any member. Add: Owner/Member (Viewers are read-only).
/// Edit/delete: the author, or the project Owner (moderation).
/// </summary>
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/comments")]
[Authorize]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
public class CommentsController(ICommentService comments) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<CommentDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid projectId, Guid taskId, [FromQuery] PageQuery query)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;

        var page = await comments.ListAsync(access, taskId, query);
        return page is null ? NotFoundError("Task not found") : Ok(ApiResponse.Ok(page));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<CommentDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid projectId, Guid taskId, CommentRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectContributor);
        if (denied is not null) return denied;

        var comment = await comments.CreateAsync(access, Caller, taskId, request);
        return comment is null
            ? NotFoundError("Task not found")
            : Created($"/api/projects/{projectId}/tasks/{taskId}/comments/{comment.Id}", ApiResponse.Ok(comment));
    }

    [HttpPatch("{commentId:guid}")]
    [ProducesResponseType<ApiResponse<CommentDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid projectId, Guid taskId, Guid commentId, CommentRequest request)
    {
        if (await AuthorizeCommentAsync(projectId, taskId, commentId) is { } denied) return denied;
        return Ok(ApiResponse.Ok(await comments.UpdateAsync(Caller, commentId, request)));
    }

    [HttpDelete("{commentId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId, Guid commentId)
    {
        if (await AuthorizeCommentAsync(projectId, taskId, commentId) is { } denied) return denied;
        await comments.DeleteAsync(Caller, commentId);
        return NoContent();
    }

    /// <summary>Member of the project? (else 404) → comment exists under this task? (else 404) → author or Owner? (else 403)</summary>
    private async Task<IActionResult?> AuthorizeCommentAsync(Guid projectId, Guid taskId, Guid commentId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;

        var resource = await comments.FindAsync(access, taskId, commentId);
        if (resource is null) return NotFoundError("Comment not found");

        return await AuthorizeResourceAsync(resource, Policies.CommentAuthorOrProjectOwner);
    }
}

/// <summary>
/// Time logs on a task. Same rules as comments; the log's user is always the caller.
/// </summary>
[Route("api/projects/{projectId:guid}/tasks/{taskId:guid}/timelogs")]
[Authorize]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
public class TimeLogsController(ITimeLogService timeLogs) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<TimeLogDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(Guid projectId, Guid taskId, [FromQuery] PageQuery query)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;

        var page = await timeLogs.ListAsync(access, taskId, query);
        return page is null ? NotFoundError("Task not found") : Ok(ApiResponse.Ok(page));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<TimeLogDto>>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(Guid projectId, Guid taskId, CreateTimeLogRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectContributor);
        if (denied is not null) return denied;

        var log = await timeLogs.CreateAsync(access, Caller, taskId, request);
        return log is null
            ? NotFoundError("Task not found")
            : Created($"/api/projects/{projectId}/tasks/{taskId}/timelogs/{log.Id}", ApiResponse.Ok(log));
    }

    [HttpPatch("{logId:guid}")]
    [ProducesResponseType<ApiResponse<TimeLogDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Update(Guid projectId, Guid taskId, Guid logId, UpdateTimeLogRequest request)
    {
        if (await AuthorizeLogAsync(projectId, taskId, logId) is { } denied) return denied;
        return Ok(ApiResponse.Ok(await timeLogs.UpdateAsync(Caller, logId, request)));
    }

    [HttpDelete("{logId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId, Guid logId)
    {
        if (await AuthorizeLogAsync(projectId, taskId, logId) is { } denied) return denied;
        await timeLogs.DeleteAsync(Caller, logId);
        return NoContent();
    }

    private async Task<IActionResult?> AuthorizeLogAsync(Guid projectId, Guid taskId, Guid logId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;

        var resource = await timeLogs.FindAsync(access, taskId, logId);
        if (resource is null) return NotFoundError("Time log not found");

        return await AuthorizeResourceAsync(resource, Policies.TimeLogAuthorOrProjectOwner);
    }
}
