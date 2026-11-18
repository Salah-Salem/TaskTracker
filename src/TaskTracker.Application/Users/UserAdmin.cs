using FluentValidation;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Domain;

namespace TaskTracker.Application.Users;

public record ChangeRoleRequest(string Role);

public class ChangeRoleRequestValidator : AbstractValidator<ChangeRoleRequest>
{
    public ChangeRoleRequestValidator()
    {
        RuleFor(x => x.Role)
            .NotEmpty()
            .Must(r => Roles.All.Contains(r))
            .WithMessage($"Role must be one of: {string.Join(", ", Roles.All)}");
    }
}

/// <summary>US-B2: Admin-only user management.</summary>
public interface IUserAdminService
{
    Task<PagedResult<UserDto>> ListAsync(PageQuery query);

    /// <param name="actorId">The Admin performing the action (for the audit log).</param>
    Task<ServiceResult<UserDto>> ChangeRoleAsync(Guid actorId, Guid userId, string role);
    Task<ServiceResult<UserDto>> SetActiveAsync(Guid actorId, Guid userId, bool isActive);
}
