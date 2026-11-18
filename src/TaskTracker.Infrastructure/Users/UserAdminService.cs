using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaskTracker.Application.Auth;
using TaskTracker.Application.Common;
using TaskTracker.Application.Users;
using TaskTracker.Domain;
using TaskTracker.Domain.Entities;
using TaskTracker.Infrastructure.Auth;
using TaskTracker.Infrastructure.Persistence;

namespace TaskTracker.Infrastructure.Users;

public class UserAdminService(
    AppDbContext db,
    UserManager<ApplicationUser> users,
    ILogger<UserAdminService> log) : IUserAdminService
{
    public const string LastAdmin = "At least one active Admin is required";
    public const string UserNotFound = "User not found";

    public async Task<PagedResult<UserDto>> ListAsync(PageQuery query)
    {
        var ordered = db.Users.AsNoTracking().OrderBy(u => u.Email);
        var total = await ordered.CountAsync();

        var rows = await ordered
            .Skip(query.Skip).Take(query.PageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.DisplayName, u.IsActive,
                Roles = db.UserRoles.Where(ur => ur.UserId == u.Id)
                    .Join(db.Roles, ur => ur.RoleId, r => r.Id, (_, r) => r.Name!)
                    .ToList(),
            })
            .ToListAsync();

        var items = rows.Select(r => new UserDto(r.Id, r.Email!, r.DisplayName, r.Roles.ToArray(), r.IsActive)).ToList();
        return new PagedResult<UserDto>(items, query.Page, query.PageSize, total);
    }

    public async Task<ServiceResult<UserDto>> ChangeRoleAsync(Guid actorId, Guid userId, string role)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult<UserDto>.NotFound(UserNotFound);

        var current = await users.GetRolesAsync(user);
        if (current.Count == 1 && current[0] == role)
            return ServiceResult<UserDto>.Ok(ToDto(user, [role])); // no-op; keep existing sessions

        if (role != Roles.Admin && await IsLastActiveAdminAsync(user, current))
            return ServiceResult<UserDto>.BadRequest(LastAdmin);

        // One role per user in this POC: replace whatever they had.
        await Check(users.RemoveFromRolesAsync(user, current));
        await Check(users.AddToRoleAsync(user, role));
        await Check(users.UpdateSecurityStampAsync(user)); // old tokens carry the old role -> invalidate them

        log.LogInformation("Audit: admin {ActorId} changed role of user {UserId} from [{OldRoles}] to {NewRole}",
            actorId, user.Id, string.Join(",", current), role);
        return ServiceResult<UserDto>.Ok(ToDto(user, [role]));
    }

    public async Task<ServiceResult<UserDto>> SetActiveAsync(Guid actorId, Guid userId, bool isActive)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null) return ServiceResult<UserDto>.NotFound(UserNotFound);

        var roles = await users.GetRolesAsync(user);
        if (user.IsActive == isActive)
            return ServiceResult<UserDto>.Ok(ToDto(user, roles.ToArray()));

        if (!isActive && await IsLastActiveAdminAsync(user, roles))
            return ServiceResult<UserDto>.BadRequest(LastAdmin);

        user.IsActive = isActive;
        await Check(users.UpdateAsync(user));
        // The per-request IsActive check already rejects their tokens; rotating the stamp and revoking
        // refresh tokens also means re-activation does not revive any session from before deactivation.
        await Check(users.UpdateSecurityStampAsync(user));
        if (!isActive)
            await AuthService.RevokeAllRefreshTokensAsync(db, user.Id, DateTime.UtcNow);

        log.LogInformation("Audit: admin {ActorId} {Action} user {UserId}",
            actorId, isActive ? "activated" : "deactivated", user.Id);
        return ServiceResult<UserDto>.Ok(ToDto(user, roles.ToArray()));
    }

    private async Task<bool> IsLastActiveAdminAsync(ApplicationUser user, IList<string> roles)
    {
        if (!user.IsActive || !roles.Contains(Roles.Admin)) return false;
        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        return admins.Count(a => a.IsActive) <= 1;
    }

    private static async Task Check(Task<IdentityResult> op)
    {
        var r = await op;
        if (!r.Succeeded)
            throw new InvalidOperationException("Identity operation failed: " +
                string.Join("; ", r.Errors.Select(e => e.Code)));
    }

    private static UserDto ToDto(ApplicationUser u, string[] roles) =>
        new(u.Id, u.Email!, u.DisplayName, roles, u.IsActive);
}
