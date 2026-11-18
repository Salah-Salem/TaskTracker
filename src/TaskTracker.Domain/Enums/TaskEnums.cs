namespace TaskTracker.Domain.Enums;

// Note: TaskStatus collides with System.Threading.Tasks.TaskStatus (implicit using).
// Consumers should alias: using TaskStatus = TaskTracker.Domain.Enums.TaskStatus;
public enum TaskStatus { Todo = 0, InProgress = 1, InReview = 2, Done = 3, Cancelled = 4 }

public enum TaskPriority { Low = 0, Medium = 1, High = 2, Critical = 3 }

/// <summary>Per-project role. Rank matters: a higher role implies every permission of the lower ones.</summary>
public enum ProjectRole { Viewer = 1, Member = 2, Owner = 3 }
