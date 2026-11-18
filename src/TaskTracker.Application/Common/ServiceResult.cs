namespace TaskTracker.Application.Common;

public enum ResultStatus { Ok, BadRequest, Unauthorized, Forbidden, NotFound, Conflict, Unprocessable }

/// <summary>Outcome of an application-service call; controllers map it to HTTP + ApiResponse.</summary>
public sealed record ServiceResult<T>(ResultStatus Status, T? Data, IReadOnlyList<string> Errors)
{
    public bool IsSuccess => Status == ResultStatus.Ok;

    public static ServiceResult<T> Ok(T data) => new(ResultStatus.Ok, data, []);
    public static ServiceResult<T> BadRequest(params string[] errors) => new(ResultStatus.BadRequest, default, errors);
    public static ServiceResult<T> Unauthorized(params string[] errors) => new(ResultStatus.Unauthorized, default, errors);
    public static ServiceResult<T> Forbidden(params string[] errors) => new(ResultStatus.Forbidden, default, errors);
    public static ServiceResult<T> NotFound(params string[] errors) => new(ResultStatus.NotFound, default, errors);
    /// <summary>409 — invariant violated (last Owner, duplicate membership, ...).</summary>
    public static ServiceResult<T> Conflict(params string[] errors) => new(ResultStatus.Conflict, default, errors);
    /// <summary>422 — well-formed but breaks a business rule (e.g. assignee is a Viewer).</summary>
    public static ServiceResult<T> Unprocessable(params string[] errors) => new(ResultStatus.Unprocessable, default, errors);
}
