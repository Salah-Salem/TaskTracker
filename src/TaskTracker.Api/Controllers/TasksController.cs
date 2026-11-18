using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Api.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Application.Tasks;

namespace TaskTracker.Api.Controllers;

/// <summary>
/// Tasks inside a project. Read: any member. Create/edit: Owner or Member (ProjectContributor).
/// Delete: Owner only — the Member-can-edit-but-not-delete split is what proves the two policies differ.
/// </summary>
[Route("api/projects/{projectId:guid}/tasks")]
[Authorize]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
public class TasksController(ITaskService tasks) : ApiControllerBase
{
    private const string TaskNotFound = "Task not found";

    /// <summary>Search (title/description), filter (status, priority, assigneeId|none, due range), sort, page.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<TaskDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(Guid projectId, [FromQuery] TaskListQuery query)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;
        return Ok(ApiResponse.Ok(await tasks.ListAsync(access, query)));
    }

    [HttpGet("{taskId:guid}")]
    [ProducesResponseType<ApiResponse<TaskDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid projectId, Guid taskId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;

        var task = await tasks.GetAsync(access, taskId);
        return task is null ? NotFoundError(TaskNotFound) : Ok(ApiResponse.Ok(task));
    }

    [HttpPost]
    [ProducesResponseType<ApiResponse<TaskDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Create(Guid projectId, CreateTaskRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectContributor);
        if (denied is not null) return denied;

        var result = await tasks.CreateAsync(access, Caller, request);
        return result.IsSuccess
            ? CreatedAtAction(nameof(Get), new { projectId, taskId = result.Data!.Id }, ApiResponse.Ok(result.Data))
            : FromResult(result);
    }

    /// <summary>Partial update. Send <c>"assigneeId": null</c> to unassign. Entering Done sets completedAt.</summary>
    [HttpPatch("{taskId:guid}")]
    [ProducesResponseType<ApiResponse<TaskDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> Update(Guid projectId, Guid taskId, UpdateTaskRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectContributor);
        if (denied is not null) return denied;
        return FromResult(await tasks.UpdateAsync(access, Caller, taskId, request));
    }

    [HttpDelete("{taskId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid projectId, Guid taskId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;
        return await tasks.DeleteAsync(access, Caller, taskId) ? NoContent() : NotFoundError(TaskNotFound);
    }
}

[Route("api/me")]
[Authorize]
public class MeController(ITaskService tasks) : ApiControllerBase
{
    /// <summary>Tasks assigned to me across every project I belong to (same filters as a project's list, minus assigneeId).</summary>
    [HttpGet("tasks")]
    [ProducesResponseType<ApiResponse<PagedResult<TaskDto>>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> MyTasks([FromQuery] TaskFilterQuery query) =>
        Ok(ApiResponse.Ok(await tasks.ListMineAsync(Caller, query)));
}
