namespace TaskTracker.Domain;

/// <summary>
/// Global (system-wide) roles. Day-to-day permissions come from the per-project
/// <see cref="Enums.ProjectRole"/>; the global role only distinguishes system Admins.
/// </summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string User = "User";

    public static readonly string[] All = [Admin, User];
}
