using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using TaskTracker.Application.Common;

namespace TaskTracker.Infrastructure.Common;

internal static class QueryExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PageQuery page)
    {
        var total = await query.CountAsync();
        var items = await query.Skip(page.Skip).Take(page.PageSize).ToListAsync();
        return new PagedResult<T>(items, page.Page, page.PageSize, total);
    }

    public static IOrderedQueryable<T> OrderByDir<T, TKey>(this IQueryable<T> q, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? q.OrderByDescending(key) : q.OrderBy(key);

    public static IOrderedQueryable<T> ThenByDir<T, TKey>(this IOrderedQueryable<T> q, Expression<Func<T, TKey>> key, bool descending) =>
        descending ? q.ThenByDescending(key) : q.ThenBy(key);
}
