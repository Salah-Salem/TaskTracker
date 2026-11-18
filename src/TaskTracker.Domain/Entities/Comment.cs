namespace TaskTracker.Domain.Entities;

public class Comment
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = default!;
    public Guid AuthorId { get; set; }
    public ApplicationUser Author { get; set; } = default!;
    public string Body { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class TimeLog
{
    public Guid Id { get; set; }
    public Guid TaskId { get; set; }
    public TaskItem Task { get; set; } = default!;
    /// <summary>Always the caller — nobody can log time on behalf of someone else.</summary>
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = default!;
    public int Minutes { get; set; }
    public DateOnly WorkDate { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = default!;
    /// <summary>SHA-256 of the token; the raw value is only ever returned to the client once.</summary>
    public string TokenHash { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public Guid? ReplacedByTokenId { get; set; }
}
