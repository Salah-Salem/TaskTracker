using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Application.Common;
using TaskTracker.Application.Projects;
using TaskTracker.Domain;
using TaskTracker.Infrastructure.Auth;

namespace TaskTracker.Api.Controllers;

[ApiController]
[Produces("application/json")]
public abstract class ApiControllerBase : ControllerBase
{
    /// <summary>Current user's id from the "sub" claim. Only valid on [Authorize] endpoints.</summary>
    protected Guid CurrentUserId => Guid.Parse(User.FindFirst(AuthClaimTypes.Subject)!.Value);

    protected CurrentUser Caller => new(CurrentUserId, User.IsInRole(Roles.Admin));

    protected IActionResult FromResult<T>(ServiceResult<T> r) => r.Status switch
    {
        ResultStatus.Ok            => Ok(ApiResponse.Ok(r.Data)),
        ResultStatus.BadRequest    => BadRequest(ApiResponse.Fail(r.Errors)),
        ResultStatus.Unauthorized  => StatusCode(StatusCodes.Status401Unauthorized, ApiResponse.Fail(r.Errors)),
        ResultStatus.Forbidden     => Forbidden(r.Errors.ToArray()),
        ResultStatus.NotFound      => NotFound(ApiResponse.Fail(r.Errors)),
        ResultStatus.Conflict      => Conflict(ApiResponse.Fail(r.Errors)),
        ResultStatus.Unprocessable => UnprocessableEntity(ApiResponse.Fail(r.Errors)),
        _ => throw new ArgumentOutOfRangeException(nameof(r)),
    };

    protected IActionResult Forbidden(params string[] errors) =>
        StatusCode(StatusCodes.Status403Forbidden, ApiResponse.Fail(errors.Length > 0 ? errors : ["Forbidden"]));

    protected IActionResult NotFoundError(string error) => NotFound(ApiResponse.Fail(error));

    /// <summary>
    /// The resource-based check every project-scoped endpoint starts with (TASKS.md §8.2):
    /// not a member → 404 (existence is not revealed); member with too low a role → 403.
    /// Usage: <c>var (access, denied) = await AuthorizeProjectAsync(id, Policies.X); if (denied is not null) return denied;</c>
    /// </summary>
    protected async Task<(ProjectAccess Access, IActionResult? Denied)> AuthorizeProjectAsync(Guid projectId, string policy)
    {
        var projects = HttpContext.RequestServices.GetRequiredService<IProjectService>();
        var access = await projects.GetAccessAsync(projectId, Caller);
        if (access is null)
            return (null!, NotFoundError("Project not found"));

        return (access, await AuthorizeResourceAsync(access, policy));
    }

    /// <summary>Evaluates a resource-based policy; returns a 403 result when it fails, otherwise null.</summary>
    protected async Task<IActionResult?> AuthorizeResourceAsync(object resource, string policy)
    {
        var authz = HttpContext.RequestServices.GetRequiredService<IAuthorizationService>();
        var result = await authz.AuthorizeAsync(User, resource, policy);
        return result.Succeeded ? null : Forbidden();
    }
}
