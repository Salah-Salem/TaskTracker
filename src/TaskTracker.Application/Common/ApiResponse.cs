namespace TaskTracker.Application.Common;

/// <summary>Consistent response envelope (Section 7): { success, data, errors }.</summary>
public record ApiResponse<T>(bool Success, T? Data, IReadOnlyList<string> Errors);

public static class ApiResponse
{
    public static ApiResponse<T> Ok<T>(T data) => new(true, data, []);

    public static ApiResponse<object> Fail(params string[] errors) => new(false, null, errors);

    public static ApiResponse<object> Fail(IEnumerable<string> errors) => new(false, null, errors.ToArray());
}
