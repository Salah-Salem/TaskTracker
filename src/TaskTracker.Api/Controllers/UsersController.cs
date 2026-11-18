using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Application.Users;

namespace TaskTracker.Api.Controllers;

/// <summary>US-B2: Admin user management. Every action requires the Admin role (403 otherwise).</summary>
[Route("api/users")]
[Authorize(Policy = "AdminOnly")]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ApiResponse<object>>(StatusCodes.Status403Forbidden)]
public class UsersController(IUserAdminService users) : ApiControllerBase
{
    [HttpGet]
    [ProducesResponseType<ApiResponse<PagedResult<UserDto>>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] PageQuery query) =>
        Ok(ApiResponse.Ok(await users.ListAsync(query)));

    [HttpPut("{id:guid}/role")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeRoleRequest request) =>
        FromResult(await users.ChangeRoleAsync(CurrentUserId, id, request.Role));

    [HttpPut("{id:guid}/deactivate")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(Guid id) =>
        FromResult(await users.SetActiveAsync(CurrentUserId, id, isActive: false));

    [HttpPut("{id:guid}/activate")]
    [ProducesResponseType<ApiResponse<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ApiResponse<object>>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Activate(Guid id) =>
        FromResult(await users.SetActiveAsync(CurrentUserId, id, isActive: true));
}
