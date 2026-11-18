using FluentValidation;

namespace TaskTracker.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount);

/// <summary>Bound from the query string (?page=&amp;pageSize=). Base class of every list query.</summary>
public class PageQuery
{
    public const int MaxPageSize = 100;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;

    public int Skip => (Page - 1) * PageSize;
}

/// <summary>A list query that can also be sorted (?sortBy=&amp;sortDir=asc|desc).</summary>
public abstract class SortedPageQuery : PageQuery
{
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }

    public bool Descending => !string.Equals(SortDir, "asc", StringComparison.OrdinalIgnoreCase);
}

public class PageQueryValidator : AbstractValidator<PageQuery>
{
    public PageQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, PageQuery.MaxPageSize);
    }
}

public static class QueryRules
{
    /// <summary>Shared rules for <see cref="SortedPageQuery"/>: paging + sortBy whitelist + sortDir.</summary>
    public static void AddSortedPageRules<T>(this AbstractValidator<T> v, IReadOnlyCollection<string> sortFields)
        where T : SortedPageQuery
    {
        v.Include(new PageQueryValidator());
        v.RuleFor(x => x.SortBy)
            .Must(s => s is null || sortFields.Contains(s, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"sortBy must be one of: {string.Join(", ", sortFields)}");
        v.RuleFor(x => x.SortDir)
            .Must(s => s is null || s.Equals("asc", StringComparison.OrdinalIgnoreCase) || s.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithMessage("sortDir must be 'asc' or 'desc'");
    }

    /// <summary>
    /// Parses a repeatable enum filter: <c>?status=Todo&amp;status=InProgress</c> and <c>?status=Todo,InProgress</c>
    /// are both accepted, case-insensitively. Returns false on any unknown value.
    /// </summary>
    public static bool TryParseEnums<TEnum>(string[]? raw, out List<TEnum> values) where TEnum : struct, Enum
    {
        values = [];
        if (raw is null) return true;
        foreach (var part in raw.SelectMany(r => r.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)))
        {
            if (!Enum.TryParse<TEnum>(part, ignoreCase: true, out var value) || !Enum.IsDefined(value) || int.TryParse(part, out _))
                return false;
            values.Add(value);
        }
        return true;
    }

    /// <summary>Escapes LIKE wildcards so user input is matched literally (used with escape char '\').</summary>
    public static string LikeContains(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
