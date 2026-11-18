using TaskTracker.Domain.Enums;
using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;

namespace TaskTracker.Domain.Entities;

public class TaskItem
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = default!;
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public TaskStatus Status { get; private set; } = TaskStatus.Todo;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public Guid? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }
    public DateOnly? DueDate { get; set; }
    public Guid CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public Guid? UpdatedById { get; set; }
    public DateTime? CompletedAt { get; private set; }
    public bool IsDeleted { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<TimeLog> TimeLogs { get; set; } = new List<TimeLog>();

    /// <summary>Transitions are free-form, but entering Done stamps CompletedAt and leaving Done clears it.</summary>
    public void SetStatus(TaskStatus status, DateTime utcNow)
    {
        if (status == Status) return;
        CompletedAt = status == TaskStatus.Done ? utcNow : null;
        Status = status;
    }
}
