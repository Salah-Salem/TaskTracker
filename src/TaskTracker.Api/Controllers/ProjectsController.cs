using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Api.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Application.Projects;

namespace TaskTracker.Api.Controllers;

/// <summary>
/// Projects + membership. Not a member → 404; member with too low a role → 403.
/// Membership invariants (last Owner, duplicates, granting Owner directly) → 409.
/// </summary>
[Route("api/projects")]
[Authorize]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
public class ProjectsController(IProjectService projects) : ApiControllerBase
{
    /// <summary>My projects (Admins: all). Search on name/description, filter by my role, sort, page.</summary>
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<ProjectDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] ProjectListQuery query) =>
        Ok(ApiResponse.Ok(await projects.ListAsync(Caller, query)));

    /// <summary>Any authenticated user may create a project and becomes its Owner.</summary>
    [HttpPost]
    [ProducesResponseType<ApiResponse<ProjectDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateProjectRequest request)
    {
        var project = await projects.CreateAsync(Caller, request);
        return CreatedAtAction(nameof(Get), new { projectId = project.Id }, ApiResponse.Ok(project));
    }

    [HttpGet("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid projectId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;
        return Ok(ApiResponse.Ok(await projects.GetAsync(access)));
    }

    /// <summary>Partial update — only the fields present are changed.</summary>
    [HttpPatch("{projectId:guid}")]
    [ProducesResponseType<ApiResponse<ProjectDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update(Guid projectId, UpdateProjectRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;
        return Ok(ApiResponse.Ok(await projects.UpdateAsync(access, Caller, request)));
    }

    [HttpDelete("{projectId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid projectId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;
        await projects.DeleteAsync(access, Caller);
        return NoContent();
    }

    [HttpGet("{projectId:guid}/members")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MemberDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Members(Guid projectId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectMember);
        if (denied is not null) return denied;
        return Ok(ApiResponse.Ok(await projects.ListMembersAsync(access)));
    }

    /// <summary>Add an existing user by email as Member or Viewer.</summary>
    [HttpPost("{projectId:guid}/members")]
    [ProducesResponseType<ApiResponse<MemberDto>>(StatusCodes.Status201Created)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddMember(Guid projectId, AddMemberRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;

        var result = await projects.AddMemberAsync(access, Caller, request);
        return result.IsSuccess
            ? Created($"/api/projects/{projectId}/members", ApiResponse.Ok(result.Data))
            : FromResult(result);
    }

    [HttpPatch("{projectId:guid}/members/{userId:guid}")]
    [ProducesResponseType<ApiResponse<MemberDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeMemberRole(Guid projectId, Guid userId, ChangeMemberRoleRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;
        return FromResult(await projects.ChangeMemberRoleAsync(access, Caller, userId, request.Role));
    }

    [HttpDelete("{projectId:guid}/members/{userId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveMember(Guid projectId, Guid userId)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;

        var result = await projects.RemoveMemberAsync(access, Caller, userId);
        return result.IsSuccess ? NoContent() : FromResult(result);
    }

    /// <summary>The new Owner must already be a member; the previous Owner becomes a Member.</summary>
    [HttpPost("{projectId:guid}/transfer-ownership")]
    [ProducesResponseType<ApiResponse<IReadOnlyList<MemberDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> TransferOwnership(Guid projectId, TransferOwnershipRequest request)
    {
        var (access, denied) = await AuthorizeProjectAsync(projectId, Policies.ProjectOwner);
        if (denied is not null) return denied;
        return FromResult(await projects.TransferOwnershipAsync(access, Caller, request.NewOwnerUserId));
    }
}
