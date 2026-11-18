using TaskTracker.Domain.Enums;

namespace TaskTracker.Domain.Entities;

public class Project
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }

    public ICollection<ProjectMember> Members { get; set; } = new List<ProjectMember>();
    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}

/// <summary>Drives all resource-based authorization. Exactly one Owner per project at all times.</summary>
public class ProjectMember
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = default!;
    public ProjectRole Role { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
}
